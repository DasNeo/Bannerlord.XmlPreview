# M0 Completion Report: Stabilize the Current Refactor

Raw validation evidence is retained outside the source repository. `{evidence-archive}` denotes the anonymized evidence archive; the documented results remain in this report.

Status: implementation and verification complete

Completed: 2026-10-01

## Implemented work packages

### 0.1 Review baseline

Every pre-existing tracked and untracked change was assigned to one reviewed group:

| Intent | Reviewed files | Review result |
| --- | --- | --- |
| Async loading and startup | `App.xaml.cs`, `ViewModels/MainWindowViewModel.cs`, `Bannerlord.XmlPreview.Core/Services/PrefabLoader.cs` | Startup awaits the initial load, background loading is cancellable between loader phases, and only the active generation publishes. No abandoned load implementation remains. |
| Responsibility extraction | `MainWindow.xaml`, `MainWindow.xaml.cs`, `Interfaces/IDiagnosticsSnapshotWriter.cs`, `Interfaces/IPreviewTreeBuilder.cs`, `Interfaces/ITemplateMockService.cs`, `Services/DiagnosticsSnapshotWriter.cs`, `Services/PreviewTreeBuilder.cs`, `Services/TemplateMockService.cs`, `ViewModels/Mocks/MockEditorViewModel.cs`, `ViewModels/PreviewInspectorViewModel.cs` | Extracted responsibilities have one DI registration and one active call path. The old inline ViewModel implementations have no remaining callers. |
| Warm-up and installation indexing | `Bannerlord.XmlPreview.Bannerlord/Adapters/Bannerlord/BannerlordWidgetReflectionService.cs`, `Bannerlord.XmlPreview.Bannerlord/Adapters/BannerlordPrefabReferenceResolver.cs`, `Bannerlord.XmlPreview.Bannerlord/Adapters/BannerlordWidgetPropertyResolver.cs`, `Bannerlord.XmlPreview.Bannerlord/Services/BannerlordAssetCatalog.cs`, `Bannerlord.XmlPreview.Core/Interfaces/IAssetCatalog.cs`, `Bannerlord.XmlPreview.Bannerlord/Interfaces/IBannerlordInstallationIndex.cs`, `Bannerlord.XmlPreview.Bannerlord/Interfaces/IPreviewEnvironmentInitializer.cs`, `Bannerlord.XmlPreview.Bannerlord/Services/BannerlordInstallationIndex.cs`, `Bannerlord.XmlPreview.Bannerlord/Services/PreviewEnvironmentInitializer.cs` | Shared snapshots are synchronized, install-root changes invalidate by key, caller cancellation does not corrupt the shared index task, and warm-up cancellation sources are replaced and disposed under a lock. |
| File watching | `Services/FileWatcherService.cs` | Registrations are synchronized, duplicate paths are rejected, notifications are debounced, and watcher plus pending cancellation resources are disposed. |
| Diagnostics and render caching | `Bannerlord.XmlPreview.Core/Rendering/PrefabRenderContext.cs` | Existing asset and pixel-bound caches remain run-local or weakly keyed as intended. Budget exhaustion now produces a structured `Partial` result. |
| Project terminology and roadmap | `GLOSSARY.md`, `docs/roadmap.md`, `docs/adr/0001-do-not-execute-mod-code-widgets.md`, all files under `docs/implementation-plans/` | These files form one intentional planning set. Terminology and the no-mod-code execution boundary are consistent with M0. |

No unrelated pre-existing change was found. Broad reset or checkout operations were not used.

### 0.2 Testable load coordination

- Added app-level `LatestLoadCoordinator<TRequest, TResult>`.
- Injected the load delegate and result/failure publishers.
- Centralized generation tracking, serialization, cancellation ownership, and cancellation-source disposal.
- Passed the cancellation token through `IPrefabLoader` and checked it between major load and recursive expansion phases.
- Removed generation, semaphore, and cancellation-source ownership from `MainWindowViewModel`.
- Added automated coverage for normal completion, replacement cancellation, stale-result suppression, active-generation failures, silent cancellation, and disposal of the replaced cancellation source.

### 0.3 Honest completion status

- Added `RenderCompletionStatus` with `Complete`, `Partial`, and `Failed`.
- Added structured status and truncation reasons to `PreviewDiagnostics` and diagnostics snapshots.
- Render failures mark diagnostics as `Failed` before propagating.
- Node-budget exhaustion marks diagnostics as `Partial` and retains the human-readable warning.

### 0.4 Authoritative Phase 9

- Phase 9 parses `CompletionStatus` rather than warning text.
- `Partial`, `Failed`, timeout, process failure, missing diagnostics, missing fixture, and invalid status are aggregate failures.
- The script writes `summary.md` and preserves fixture diagnostics before returning exit code 1.
- The six-fixture set is unchanged.
- A zero-second simulated timeout returned exit code 1 and retained its summary.

### 0.5 Refactor completion

- DI registrations match the extracted services and contain no parallel implementations.
- The ViewModel publishes only active loads and uses the UI synchronization context for publication.
- File watcher, environment warm-up, and installation-index disposal paths were reviewed.
- A follow-up race review extended cancellation-source ownership until each load, debounce, or warm-up task finishes. Faulted warm-up and index tasks can be retried. Load failures now start fresh diagnostics instead of retaining a previous failure.
- README paths and commands now match the four production projects plus the test project.
- Cover-children relayout now reuses already rendered child elements rather than recursively rebuilding identical subtrees. This removed the Tournament fixture's repeated render explosion.

## Important deviation from the plan

The Tournament fixture did not need reduced Preview Scenario list data. Review showed that `ContainerPrefabNode` rebuilt its children during cover-children relayout, causing identical nested Tournament paths to render up to 16 times. M0 fixes that generic relayout behavior and uses a verified node budget of 10,000. The unchanged Tournament data now completes without Truncation.

No M1 contract was introduced.

## Verification

Commands (run from the repository root):

```powershell
dotnet build .\Bannerlord.XmlPreview.sln --no-restore
dotnet test .\Bannerlord.XmlPreview.sln --no-build
powershell -ExecutionPolicy Bypass -File .\tools\run_phase9_validation.ps1 -PerFixtureTimeoutSeconds 30 -OutputRoot artifacts/m0-final-success
powershell -ExecutionPolicy Bypass -File .\tools\run_phase9_validation.ps1 -Fixture standard-window -PerFixtureTimeoutSeconds 0 -OutputRoot artifacts/m0-simulated-failure
```

Results:

- Build: passed, 0 warnings, 0 errors.
- Tests: 8 passed, 0 failed, 0 skipped.
- Phase 9: exit code 0; all six fixtures `Complete`; all six `TruncationReason: None`.
- Simulated timeout: exit code 1; `summary.md` retained with `Status: TimedOut`.

Final fixture render durations from the successful run:

| Fixture | RenderDurationMs | CompletionStatus |
| --- | ---: | --- |
| standard-window | 284 | Complete |
| standard-background | 156 | Complete |
| standard-scrollable-panel | 152 | Complete |
| land-management | 350 | Complete |
| tournament-scrollable-panel | 5045 | Complete |
| options-top-panel | 516 | Complete |

These measurements are recorded, not gated. The run performed technical validation only and no visual comparison.

Reverification on 2026-10-01 after the cancellation lifecycle fixes: build passed with 0 warnings and 0 errors; 8 tests passed; all six fixtures reported `Complete` and `TruncationReason: None` with exit code 0. The new summary is at `{evidence-archive}/build-verify/m0-resume-final/summary.md`. A zero-second timeout again returned exit code 1 and preserved its summary at `{evidence-archive}/build-verify/m0-resume-simulated-failure/summary.md`. The interactive smoke loaded Tournament.xml followed by Standard.Window.xml and displayed the latter as the final preview.

## Reverification before M1: 2026-10-04

The committed M0 baseline (`7938220`, following `25f6d57`) had a clean working tree before M1 edits. The load coordinator, its automated scenarios, DI call sites, structured completion handling, and Phase-9 aggregation were checked against the exit criteria.

- `dotnet build Bannerlord.XmlPreview.sln --no-restore`: passed, 0 warnings, 0 errors.
- `dotnet test Bannerlord.XmlPreview.sln --no-build`: all 8 M0 tests passed.
- Phase 9 with a 30-second per-fixture timeout: exit code 0; all six diagnostics files contain `Complete` and `TruncationReason: None`.
- Phase 9 with `standard-window` and a zero-second timeout: exit code 1; the summary records `TimedOut`.
- Successful run evidence: `{evidence-archive}/build-verify/m0-20261004-success/summary.md`.
- Failure-path evidence: `{evidence-archive}/build-verify/m0-20261004-failure/summary.md`.

The manual WPF smoke evidence remains the 2026-10-01 run documented above; it was not repeated during this reverification. M0 meets its exit criteria and M1 can start. Subsequent working-tree changes belong to M1 and this verification record.

## Known limitations and deferred work

- A Windows UI smoke opened Tournament.xml and then Standard.Window.xml in quick succession. The app displayed the final Standard.Window.xml path and `Loaded Standard.Window.xml`. Tournament rendering briefly occupied the UI thread, so this visual interaction does not prove overlap between the two loads; the automated coordination tests cover superseded in-flight work.
- Visual Regression and screenshot capture remain deferred to M1.
- Behavior Graph contracts, Core-to-DemoData removal, multi-file dependency watching, and Last Good Preview behavior remain deferred as planned.
- Existing fixture warnings remain visible and are not converted into success claims.

## Exit checklist

- [x] Build succeeds.
- [x] Automated load-coordination tests pass.
- [x] All six Phase-9 fixtures complete without Truncation.
- [x] Phase 9 returns non-zero on simulated failure.
- [x] Existing changes have been reviewed and accounted for.
- [x] Documentation matches the final structure.
- [x] Manual rapid-consecutive-load WPF smoke test (scope described above).
- [x] Git working tree clean after committing the reviewed M0 changes.

## ADRs

No new ADR was required. M0 preserves [ADR 0001](../adr/0001-do-not-execute-mod-code-widgets.md).
