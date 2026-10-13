# M2: Behavior Graph implementation and evidence

Implemented on 2026-10-12 against the M2 implementation plan. The four proof widgets passed before ADR 0002 was marked accepted. The graph has one public entry point and exactly five phases and contribution contracts; DimensionSync required no interface expansion.

## Rendering boundary

- Core no longer references DemoData or reads/writes static mock bindings. The Bannerlord adapter constructs immutable bindings, color, template-row, and Optional Connection explanation snapshots. The interactive editor owns an instance binding store; regression scenarios supply their own values.
- Scenario overrides copy the node tree instead of changing the loaded document. Parallel STA renders of the same document verify isolation.
- XML descriptions used by matchers come from the original document. Resolved scenario data is available to handlers, never matcher criteria.
- Graph planning expands keyed templates, reuses M1 stable Widget IDs, validates all phases and connections, and completes before creating WPF elements or fallback brushes.
- Stateless adapter handlers return declarations. Core owns generic WPF materialization and layout intent. Adapter-specific node primitives have moved out of Core; Core no longer dispatches behavior by concrete Bannerlord widget name.
- `ApplyDeferredReferenceBehaviors`, the visibility/state/dimension pilot methods, and the old name checks are deleted. Scroll panels use generic container fallback plus graph connections and viewport intent. The adapter retains the old scroll node type only so archived M1 corpus records remain readable.
- Shipped game binaries and explicitly shipped module directories provide trusted metadata. Third-party module DLLs are excluded from reflection discovery and dependency resolution; mod XML cannot register executable rules.
- A root is returned only after successful materialization, layout, inspection, and cancellation checks. The app retains the Last Good Preview and displays a stale status after render/load failure.

## Graph validation and Diagnostics

Tests exercise stable topological order, explicit append order, rule and Widget ID uniqueness, cycles, exclusive Instantiate conflicts, fallback, case-insensitive single-writer properties, Required/Optional/unknown connection requirements, handler failures, cancellation, and cross-widget state dependencies. Describe results and copied state travel in immutable run-local maps indexed by Widget ID.

Every planned run records installed and matched rule counts, match and edge counts, contribution/support counts, and matching/sorting/total times, including graph failures. Full traces include matcher criteria, selected/skipped rules, ordering, contributions, and support status and are generated for regression artifacts, debugging, or failures. Regression capture writes `behavior-trace.json`; ordinary successful interactive renders retain only the compact summary. Inspect contributes Diagnostics, support status, and Key Bounds selectors without changing arranged WPF elements.

## Fidelity Status

| Family | Status | Verified scope and remaining limits |
| --- | --- | --- |
| ValueBasedVisibilityWidget | Verified | Resolved numeric comparisons; isolated state and scenario overrides. |
| ScrollablePanel | Verified | Required viewport/inner-panel ports, Optional bars with scenario explanations, clipping, overflow/bar visibility, and explicit arranged bounds. |
| StateSyncWidget | Verified | Required source, explicit target or self target, immutable state copies, deterministic chained propagation and cycle/conflict rejection. |
| DimensionSyncWidget | Verified | Measured source, horizontal/vertical/both axes, padding, and explained Optional source omission. |
| Wave-1 visibility/navigation/scroll helpers | Implemented | Adapter matching and declarative intents; affected portable native cases covered. Interactive navigation and timing remain outside static preview. |
| Sliders | Verified | Min/max normalization, clamping, discrete intervals, handle travel and filler geometry; low/mid/high arranged fixtures include pixel capture. Runtime dragging is outside static preview. |
| Editable text | Implemented | Declarative text blueprint with resolved scenario text. Caret, selection, IME and editing interaction are not claimed. |
| Sort | Verified | Ascending/descending/hidden indicator presentation from scenario state. |
| Toggle | Verified | Static selected/disabled/default presentation from scenario state. |
| InputKeyVisualWidget | Partial | Text/key-label blueprint; scenarios can supply representative `KeyText`. Actual key artwork and runtime localization/input-device mapping are not claimed. Missing labels produce Partial. |
| Code and unknown widgets | Placeholder | Visible labeled fallback and Partial; no executable mod behavior. |

Verified means explicit preview behavior expectations passed. It does not imply an in-game screenshot match.

## Structural acceptance

The nine portable Native cases pass Structure Only. Original M1 metadata remains in `test-data/visual-regression/portable-baselines`; reviewed M2 metadata is a separate immutable package in `test-data/visual-regression/m2-portable-baselines`. `tools/run_visual_regression.ps1` selects it for portable runs.

Five cases were accepted with the existing CLI acceptance workflow after review of actual structure and bounds. Their changes are attributable to:

1. M1's reference resolver skipped `ClipRect`, scrollbar and handle ports. These ports now resolve, activating viewport and handle behavior; bars/handles collapse when content does not overflow.
2. Missing horizontal scrollbars now have explicit scenario explanations and exact Expected Warnings. Warnings were added only for those documented Optional Connections.
3. Repeated layout passes replaced WPF elements while snapshots still pointed to detached instances. Registration now updates the element for an existing Widget ID, so Options reports final nonzero child bounds and inherited enabled/visibility state.
4. Options and SingleQuery scenarios explicitly supply representative Confirm/Cancel key labels. Their portable fixtures do not claim native key artwork.

Unchanged Window, HintTooltip, Crosshair and Conversation expectations were carried forward. M1 metadata and external PNG references were not overwritten. Detailed local review and candidate artifacts are under `artifacts/m2-final` and `artifacts/m2-accepted-candidates` (ignored build evidence).

## Validation

- `dotnet build Bannerlord.XmlPreview.sln --no-restore -m:1`
- `dotnet test Bannerlord.XmlPreview.VisualRegression.Tests/Bannerlord.XmlPreview.VisualRegression.Tests.csproj --no-restore -m:1`: 113 passed; one optional Full theory group skipped.
- `dotnet test Bannerlord.XmlPreview.Tests/Bannerlord.XmlPreview.Tests.csproj --no-restore -m:1`: 8 passed.

Full in-game pixel comparison was not run because the calibrated Reference Package/environment variables are not configured. Portable native runs report pixels as `NotRun`; focused slider fixtures perform local WPF pixel capture. No new in-game fidelity claim is based on a regenerated preview image.

## Completion audit before M3

Reverified on 2026-10-12 with `dotnet build Bannerlord.XmlPreview.sln --no-restore -m:1` (zero warnings/errors) and `dotnet test Bannerlord.XmlPreview.sln --no-build -m:1` (121 passed, one optional Full group skipped). The sandbox test runner could not connect to its local test host; the same command passed outside the sandbox. Core source has no matches for the migrated concrete widget names, DemoData, or MockData. ADR 0002 remains accepted, and the four proof widgets and selected family tests pass.

M2 is complete within the static preview scope described above. This does not certify that every default-data installed-game preview is Complete. A fresh `tools/run_phase9_validation.ps1` run completed Window, Background, ScrollablePanel and Land Management; Tournament and Options honestly returned `Partial` for InputKeyVisualWidget instances without scenario `KeyText`. The aggregate script exited 1. Evidence is in `artifacts/m2-reverification/phase9/summary.md`. These cases are not relabeled Passed; scenario-based M2 acceptance supplies explicit representative key labels and does not claim native key artwork. Tournament also took 12.7 seconds to render, so its measured performance needs a future focused investigation.
