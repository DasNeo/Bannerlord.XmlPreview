# M4 authoring loop implementation

Date: 2026-10-13. Implementation and automated acceptance are complete. Manual WPF/external-editor smoke acceptance remains pending: the user stopped Computer Use with Escape before the smoke run. M4 is not marked fully accepted.

## Delivered behavior

- Versioned Preview Scenario repository with load/save/create/clone/rename/delete. Writes validate before an atomic replacement; invalid documents and unknown JSON fields are rejected explicitly. Default storage is `%LOCALAPPDATA%/Bannerlord.XmlPreview/Scenarios`, outside the game installation.
- Scenario controls beside the prefab controls, with an active identity/schema and saved/unsaved indicator. Bindings and list edits persist, stable list keys survive reloads, and selected scenarios apply states, resolution, asset scope, widget scope, Key Bounds selectors and Optional Connection explanations. Dirty values survive dependency reloads. Switching away from dirty values requires saving; conflicting external scenario edits are reported.
- Canonical absolute dependency sets include the root, expanded includes/templates, module-scope brush and sprite metadata, relevant module metadata, and consulted texture/package/RDC or explicit PNG paths. Module-scope metadata and candidate package dependencies are deliberately conservative, so precedence changes can be detected.
- A directory-grouped watcher atomically replaces its dependency set, debounces a batch across all files, handles change/create/delete/rename and atomic saves, tolerates missing files, releases removed registrations, and identifies changed paths. Dependency changes invalidate installation, prefab-reference, metadata and decoded-image caches.
- Load publication retains the Last Good Preview and its tree/dependency set on XML, scenario, Behavior Graph, rendering or watcher-preparation failure. The current failure and stale marker are visible; the next change or manual reload can recover. Loading and scenario validation run off the dispatcher; WPF materialization stays on its owning dispatcher. The existing latest-load coordinator cancels and suppresses obsolete requests.
- Source file/line/column annotations survive includes and logical-child expansion. Preview Tree tooltips/double-clicks, Diagnostic buttons, Behavior Traces and mapped image differences expose source navigation. `BANNERLORD_PREVIEW_EDITOR` configures an executable accepting `--goto file:line:column` (for example VS Code); otherwise the adapter copies the location. Unmapped Diagnostics use the root/error location explicitly.
- Reference/actual/side-by-side/overlay/diff views share one viewport and zoom transform. Interactive reference selection uses Visual Regression capture, masks and comparator. It is labeled as an interactive image comparison; structural comparisons are `NotRun`, and the exact default tolerance is explicitly uncalibrated. Opening a Full comparison report validates capture evidence against the saved active scenario and displays its artifacts, run mode, checks, Capture Profile, masks and applied Pixel Tolerance. Structure Only reports have pixels `NotRun` and cannot supply image views. Older Full reports without recorded tolerance require a new run. Mapped differences select their source widget in the Preview Tree.
- No embedded XML editor, automatic Baseline Acceptance, or mod-code execution was introduced.

## Automated evidence

`tools/run_m4_validation.ps1` runs the full solution and all three installed M3 structural targets. Validation uses .NET 10 on Windows and the installed v1.4.7 Bannerlord assets.

The final suite contains 20 app tests and 167 Visual Regression tests. With `BANNERLORD_ROOT` configured, 186 tests pass; the optional in-game Reference Package test is skipped. All nine portable Native cases and the three installed targets (`m3-conversation`, `m3-banner-builder`, `m3-land-management`) pass Structure Only, with pixels reported as `NotRun`. Build produces zero warnings/errors. `git diff --check` passes.

New coverage exercises scenario lifecycle and invalid-save preservation, unknown-field rejection, rendered list/state/resolution values, root/include/brush/image reload, malformed include retention/recovery, wrapped XML source locations, dirty scenario retention, invalid scenario and render failure, obsolete-load cancellation, deterministic batch debounce, real atomic save/delete/recreate, metadata and image pixel invalidation, Full artifact identity validation, masks/tolerance, five comparison modes, shared zoom, and difference selection.

Artifacts are under `artifacts/m4-validation/`. No accepted snapshots or scenario hashes were changed.

## Remaining manual acceptance

Follow [M4 Steps to Repeat (StR)](../testing/m4-steps-to-repeat.md) for setup commands, a disposable root/include fixture, scenario lifecycle and validation checks, dependency edits, source navigation, comparison checks, cancellation coverage and a result sheet.

Run the WPF app with an external editor: edit the root/include/brush/image, introduce and repair malformed XML, switch/save scenarios, follow a Diagnostic and Preview Tree source link, and inspect side-by-side/overlay views with an active reference. The automated comparison-window test loads the compiled XAML and checks modes/zoom/selection, but does not replace that manual visual smoke test. In-game pixel fidelity remains optional and unverified by this milestone.
