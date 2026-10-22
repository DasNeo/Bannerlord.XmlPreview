# M0 Implementation Plan: Stabilize the Current Refactor

Historical plan; M0 was completed and reverified on 2026-10-04. The starting point and unchecked exit list below describe the original work, not outstanding tasks. See the [plan index](README.md) for current guidance.

## Objective

Turn the current async loading, cancellation, environment warm-up, installation indexing, file watching, diagnostics timing, and responsibility extraction work into a reviewed and reproducible baseline. M0 changes no product scope and introduces no new fidelity architecture.

## Starting point

Relevant current code includes:

- `src/Bannerlord.XmlPreview/ViewModels/MainWindowViewModel.cs`: generation-based load cancellation and UI publication
- `src/Bannerlord.XmlPreview/Services/FileWatcherService.cs`: debounced single-file watching
- `src/Bannerlord.XmlPreview.Bannerlord/Services/PreviewEnvironmentInitializer.cs`: background warm-up
- `src/Bannerlord.XmlPreview.Bannerlord/Services/BannerlordInstallationIndex.cs`: installation inventory
- `src/Bannerlord.XmlPreview.Core/Services/PrefabLoader.cs`: synchronous prefab loading
- `src/Bannerlord.XmlPreview.Core/Services/PrefabRenderer.cs`: render orchestration and phase timing
- `tools/run_phase9_validation.ps1`: six-fixture technical validation

The current Phase-9 script records failures in `summary.md` but does not aggregate them into a final non-zero exit code. Large prefabs can also be reported as completed after Truncation.

## Work package 0.1: Establish the review baseline

1. Inventory every tracked and untracked change in the repository.
2. Group changes by intent: async loading, responsibility extraction, warm-up/indexing, file watching, diagnostics, and unrelated work.
3. Review each group for incomplete call-site migration, duplicate implementations, stale registrations, and accidental behavior changes.
4. Preserve all intentional work. Do not use broad reset or checkout operations.
5. Record the reviewed file groups in the M0 completion report.

Deliverable: an explicit review checklist that accounts for every pre-existing change.

## Work package 0.2: Make load coordination testable

1. Define observable load-coordination behavior: cancellation propagation, latest-load-wins publication, and no publication from cancelled work.
2. Test `MainWindowViewModel` through its current dependencies when practical.
3. If the ViewModel cannot be tested without WPF plumbing, extract only the load-coordination logic into a small app-level module. Do not move UI orchestration into Core.
4. Inject the loader or load delegate instead of constructing work inside the coordinator.
5. Keep generation tracking and cancellation ownership in one place.
6. Verify disposal of replaced `CancellationTokenSource` instances.

Required automated scenarios:

- first load completes normally
- second load cancels a slow first load
- cancelled first load cannot publish after the second load
- loader failure publishes only for the active generation
- cancellation does not appear as a user-visible failure

Deliverable: automated integration coverage for the existing async contract.

## Work package 0.3: Introduce honest technical completion status

1. Add a structured render completion status to `PreviewDiagnostics` rather than inferring it from warning text.
2. Represent at least `Complete`, `Partial`, and `Failed`.
3. Mark node-budget exhaustion and any skipped subtree as Truncation and `Partial`.
4. Include the status and Truncation reason in diagnostics output.
5. Keep human-readable warnings, but make scripts consume the structured status field.

Deliverable: diagnostics that cannot report a truncated render as complete.

## Work package 0.4: Make Phase 9 authoritative

Update `tools/run_phase9_validation.ps1`:

1. Parse the structured completion status from each diagnostics file.
2. Report `Partial` distinctly from `Completed` and `Failed`.
3. Treat timeout, process failure, missing diagnostics, `Partial`, and `Failed` as aggregate failures.
4. Return a non-zero process exit code when any fixture fails.
5. Preserve all per-fixture diagnostics and the summary even on failure.
6. Keep the current six-fixture set unchanged for M0.
7. For the tournament fixture, either reduce representative list data or use a verified render budget that completes without Truncation.

Deliverable: one command whose exit code truthfully represents the six-fixture run.

## Work package 0.5: Complete the refactor review

Review these integration points:

- DI registrations in `src/Bannerlord.XmlPreview/App.xaml.cs`
- cancellation and dispatcher use in `src/Bannerlord.XmlPreview/ViewModels/MainWindowViewModel.cs`
- thread safety and disposal in warm-up and indexing modules
- watcher registration disposal and duplicate-registration behavior
- diagnostics writer and preview-tree extraction call sites
- stale README paths and command examples

Remove abandoned parallel code only after confirming no caller remains.

## Work package 0.6: Verify and close

Run from the repository root:

```powershell
dotnet build ./Bannerlord.XmlPreview.sln --no-restore
dotnet test ./Bannerlord.XmlPreview.sln --no-build
powershell -ExecutionPolicy Bypass -File ./tools/run_phase9_validation.ps1 -PerFixtureTimeoutSeconds 30 -OutputRoot ./artifacts/m0-validation
```

Then:

1. Inspect the six diagnostics files and summary.
2. Exercise rapid consecutive prefab loads manually once as a WPF smoke test.
3. Update the repository README and M0 completion report.
4. Split commits by coherent intent when practical.
5. End with a clean Git working tree.

## Exit checklist

- [ ] Build succeeds.
- [ ] Automated load-coordination tests pass.
- [ ] All six Phase-9 fixtures complete without Truncation.
- [ ] Phase 9 returns non-zero on simulated failure.
- [ ] Existing changes have been reviewed and accounted for.
- [ ] Documentation matches the final structure.
- [ ] Git working tree is clean.

## Explicitly deferred

- Visual Regression and screenshot capture
- Behavior Graph contracts
- removal of Core to DemoData dependency
- multi-file dependency watching
- Last Good Preview behavior after ordinary load failure
