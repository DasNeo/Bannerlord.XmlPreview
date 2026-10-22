# Development guide

`Bannerlord.XmlPreview` is a WPF tool for loading, expanding, and rendering Bannerlord Gauntlet prefabs from XML. It provides an inspectable preview for investigating layout, asset, binding, and reference issues without launching the game.

## Solution

- `src/Bannerlord.XmlPreview/Bannerlord.XmlPreview.csproj`: WPF host, DI, UI, and startup arguments
- `src/Bannerlord.XmlPreview.Core/`: generic loading, rendering, node, and Diagnostics logic
- `src/Bannerlord.XmlPreview.Bannerlord/`: Bannerlord-specific adapters, assets, and preview data
- `src/Bannerlord.XmlPreview.DemoData/`: deterministic preview data
- `tests/Bannerlord.XmlPreview.Tests/`: application contract and Diagnostics tests
- `src/Bannerlord.XmlPreview.VisualRegression/`: scenarios, snapshots, capture, comparators, reports, and Baseline Acceptance
- `src/Bannerlord.XmlPreview.VisualRegression.Cli/`: compare, capture, accept, calibrate, and benchmark commands
- `tests/Bannerlord.XmlPreview.VisualRegression.Tests/`: comparison, capture, and workflow tests, plus nine Native cases

Core remains generic. Bannerlord names and semantics belong in the adapter. Mod-specific executable widget code is not loaded; see [ADR 0001](../adr/0001-do-not-execute-mod-code-widgets.md).

Run commands below from the repository root. See [Contributing](../../CONTRIBUTING.md) for prerequisites and the repository layout.

## Create a release

In GitHub, open **Actions → Create Release → Run workflow**, select the branch or tag to release, enter a version tag such as `v0.1.0-preview.1`, and choose whether it is a prerelease (default: yes). The workflow runs only when dispatched manually; pushes and tags do not trigger it. The workflow must be present on the default branch to appear in the manual-run UI.

The workflow builds and tests Release, runs the nine portable Structure Only cases, and creates a GitHub release with two Windows x64 ZIPs, both using ReadyToRun:

- `Bannerlord.XmlPreview-<tag>-win-x64-framework-dependent.zip`: smaller download; requires **.NET 10 Desktop Runtime x64** installed separately.
- `Bannerlord.XmlPreview-<tag>-win-x64-self-contained.zip`: larger download; includes the .NET desktop runtime.

Choose one ZIP, extract it, and run `Bannerlord.XmlPreview.exe`. Each ZIP has a matching SHA-256 checksum and includes third-party notices/licenses and the fidelity matrix, but no game assets. A new tag points to the selected run's commit; an existing tag must already point to that commit. Existing releases are not overwritten. The built-in GitHub token supplies release permissions; no extra secret is required.

## Technical regression

Build/test/start commands are in [Contributing](../../CONTRIBUTING.md). App controls and command-line arguments are in the [README](../../readme.md).

Run automated authoring tests and the three installed image-family Structure Only comparisons:

```powershell
./tools/run_m4_validation.ps1 `
  -BannerlordRoot 'C:\Program Files (x86)\Steam\steamapps\common\Mount & Blade II Bannerlord'
```

This delegates to the image validation runner, which also supports publishing the fidelity matrix:

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\run_m3_validation.ps1 `
  -BannerlordRoot "C:\Program Files (x86)\Steam\steamapps\common\Mount & Blade II Bannerlord"
```

This runs the solution tests, nine portable renderer cases, installed package assertions, and all three installed structural targets. The [image scenarios](../../test-data/visual-regression/m3-scenarios/README.md) explain explicit provider data, active asset module scope, Diagnostics and runtime-only limits. `-PublishFidelity` updates the local fidelity matrix only after validation passes.

For interactive authoring checks, use the [manual testing guide](../testing/m4-steps-to-repeat.md). It includes a disposable fixture, expected outcomes, and a result sheet. For the leaderboard-specific layout and font checks, see [Tournament Leaderboard regression](../testing/tournament-leaderboard.md).

Phase 9 checks six curated fixtures. `Complete` indicates success; timeouts, process failures, missing Diagnostics, `Partial`, and `Failed` produce a nonzero aggregate exit code. Per-fixture Diagnostics and `summary.md` are preserved even when validation fails.

`tools/open_phase0_fixture.ps1` and `tools/run_phase9_validation.ps1` use the archived version at [`fixtures/AgricultureEstate/GUI/Prefabs/LandManagement.xml`](../../fixtures/AgricultureEstate/GUI/Prefabs/LandManagement.xml) for the `land-management` fixture. Its provenance and dependencies are described in the [fixture documentation](../../fixtures/README.md).

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\run_phase9_validation.ps1 `
  -PerFixtureTimeoutSeconds 30 `
  -OutputRoot C:\path\to\m0-validation
```

Diagnostics include `CompletionStatus`, `TruncationReason`, `RootActual`, `ElementLayout`, and phase timings. This run performs technical validation; visual acceptance is assessed separately.

## Architecture and planning

- [Glossary](../../GLOSSARY.md)
- [Roadmap](../roadmap.md)
- [Milestone plans](../implementation-plans/README.md)
- [Architecture decisions](../adr/)
- [Image fidelity matrix](../fidelity/images.md).
- [Visual Regression workflow](../../test-data/visual-regression/README.md): nine scenarios, portable CI, Reference Packages, and CLI commands.

Key entry points are `src/Bannerlord.XmlPreview/App.xaml.cs`, `src/Bannerlord.XmlPreview/ViewModels/MainWindowViewModel.cs`, `src/Bannerlord.XmlPreview.Core/Services/PrefabLoader.cs`, `src/Bannerlord.XmlPreview.Core/Services/PrefabRenderer.cs`, and `src/Bannerlord.XmlPreview.Core/Rendering/PrefabRenderContext.cs`.
