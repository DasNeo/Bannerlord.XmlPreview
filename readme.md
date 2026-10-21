# Bannerlord.XmlPreview

`Bannerlord.XmlPreview` is a WPF tool for loading, expanding, and rendering Bannerlord Gauntlet prefabs from XML. It provides an inspectable preview for investigating layout, asset, binding, and reference issues without launching the game.

All project documentation and Markdown files must be written in English.

## Solution

- `Bannerlord.XmlPreview.csproj`: WPF host, DI, UI, and startup arguments
- `Bannerlord.XmlPreview.Core/`: generic loading, rendering, node, and Diagnostics logic
- `Bannerlord.XmlPreview.Bannerlord/`: Bannerlord-specific adapters, assets, and preview data
- `Bannerlord.XmlPreview.DemoData/`: deterministic preview data
- `Bannerlord.XmlPreview.Tests/`: automated M0 contract and Diagnostics tests
- `Bannerlord.XmlPreview.VisualRegression/`: scenarios, snapshots, capture, comparators, reports, and Baseline Acceptance
- `Bannerlord.XmlPreview.VisualRegression.Cli/`: compare, capture, accept, calibrate, and benchmark commands
- `Bannerlord.XmlPreview.VisualRegression.Tests/`: comparison, capture, and workflow tests, plus nine Native cases

Core remains generic. Bannerlord names and semantics belong in the adapter. Mod-specific executable widget code is not loaded; see [ADR 0001](docs/adr/0001-do-not-execute-mod-code-widgets.md).

## Build and tests

```powershell
dotnet restore C:\Dev\WPF\Bannerlord.XmlPreview\Bannerlord.XmlPreview.sln
dotnet build C:\Dev\WPF\Bannerlord.XmlPreview\Bannerlord.XmlPreview.sln --no-restore
dotnet test C:\Dev\WPF\Bannerlord.XmlPreview\Bannerlord.XmlPreview.sln --no-build
```

## Create a release

In GitHub, open **Actions → Create Release → Run workflow**, select the branch or tag to release, enter a version tag such as `v0.1.0-preview.1`, and choose whether it is a prerelease (default: yes). The workflow runs only when dispatched manually; pushes and tags do not trigger it. The workflow must be present on the default branch to appear in the manual-run UI.

The workflow builds and tests Release, runs the nine portable Structure Only cases, and creates a GitHub release with two Windows x64 ZIPs, both using ReadyToRun:

- `Bannerlord.XmlPreview-<tag>-win-x64-framework-dependent.zip`: smaller download; requires **.NET 10 Desktop Runtime x64** installed separately.
- `Bannerlord.XmlPreview-<tag>-win-x64-self-contained.zip`: larger download; includes the .NET desktop runtime.

Choose one ZIP, extract it, and run `Bannerlord.XmlPreview.exe`. Each ZIP has a matching SHA-256 checksum and includes third-party notices/licenses and the fidelity matrix, but no game assets. A new tag points to the selected run's commit; an existing tag must already point to that commit. Existing releases are not overwritten. The built-in GitHub token supplies release permissions; no extra secret is required.

## Run the app

```powershell
C:\Dev\WPF\Bannerlord.XmlPreview\bin\Debug\net10.0-windows\Bannerlord.XmlPreview.exe
```

Supported arguments:

- `--prefab <path>` loads a prefab directly.
- `--bannerlord-root <path>` sets the Bannerlord installation path.
- `--diagnostics-out <path>` writes a Diagnostics snapshot.
- `--exit-after-load` exits the app after the technical validation run.

Example:

```powershell
C:\Dev\WPF\Bannerlord.XmlPreview\bin\Debug\net10.0-windows\Bannerlord.XmlPreview.exe `
  --bannerlord-root "C:\Program Files (x86)\Steam\steamapps\common\Mount & Blade II Bannerlord" `
  --prefab "C:\Program Files (x86)\Steam\steamapps\common\Mount & Blade II Bannerlord\Modules\Native\GUI\Prefabs\Standard\Standard.Window.xml"
```

## Technical regression

M4 adds persistent scenario controls, dependency-aware reload, Last Good Preview retention, source navigation, and image comparison views. Scenarios live in `%LOCALAPPDATA%/Bannerlord.XmlPreview/Scenarios`; edit their JSON in your external editor for widget states, masks and Capture Profile metadata. Bindings/list edits and preview resolution are editable in the app; save dirty values before switching scenarios. Set `BANNERLORD_PREVIEW_EDITOR` to an editor executable supporting `--goto file:line:column`; without it source actions copy the location.

**Compare Reference** opens an interactive image comparison using the current scenario; its uncalibrated default cannot certify in-game fidelity. **Open Comparison Report** opens artifacts from a Full Visual Regression run for the saved active scenario. Both offer reference, actual, side-by-side, overlay and diff with shared zoom/pan and mapped Key Bounds navigation. A Structure Only report has pixels `NotRun` and no image comparison views.

Run `tools/run_m4_validation.ps1` for automated authoring coverage and the installed structural targets. [M4 is complete and accepted](docs/reports/m4-completion.md) using combined automated, installed structural and live smoke evidence; copied-game asset mutation checks remain optional.

For repeatable manual testing, follow [M4 Steps to Repeat (StR)](docs/testing/m4-steps-to-repeat.md), including the disposable fixture setup, expected outcomes and result sheet.

Assets load directly from the installed module TPAC packages. Editable mod packages with the verified single-texture RDC layout use their RuntimeDataCache companion. Production setup requires no manual DDS export or cropped PNG directory. Provider images need explicit scenario data; unavailable runtime content remains visibly Partial.

M3 image and asset acceptance:

```powershell
powershell -ExecutionPolicy Bypass -File C:\Dev\WPF\Bannerlord.XmlPreview\tools\run_m3_validation.ps1 `
  -BannerlordRoot "C:\Program Files (x86)\Steam\steamapps\common\Mount & Blade II Bannerlord"
```

This runs focused image/pixel tests, nine portable renderer cases, installed package assertions, and all three M3 structural targets. The [M3 scenarios](test-data/visual-regression/m3-scenarios/README.md) explain explicit provider data, active asset module scope, diagnostics and runtime-only limits. `-PublishFidelity` updates the local fidelity matrix only after validation passes.

Phase 9 checks six curated fixtures. `Complete` indicates success; timeouts, process failures, missing Diagnostics, `Partial`, and `Failed` produce a nonzero aggregate exit code. Per-fixture Diagnostics and `summary.md` are preserved even when validation fails.

Both fixture scripts use the archived version at [`fixtures/AgricultureEstate/GUI/Prefabs/LandManagement.xml`](fixtures/AgricultureEstate/GUI/Prefabs/LandManagement.xml) for the `land-management` fixture. Its provenance and dependencies are described in the [fixture documentation](fixtures/README.md).

```powershell
powershell -ExecutionPolicy Bypass -File C:\Dev\WPF\Bannerlord.XmlPreview\tools\run_phase9_validation.ps1 `
  -PerFixtureTimeoutSeconds 30 `
  -OutputRoot C:\path\to\m0-validation
```

Diagnostics include `CompletionStatus`, `TruncationReason`, `RootActual`, `ElementLayout`, and phase timings. This run performs technical validation; visual acceptance is assessed separately.

## Architecture and planning

- [Glossary](GLOSSARY.md)
- [Roadmap](docs/roadmap.md)
- [Milestone plans](docs/implementation-plans/README.md)
- [M0 completion report](docs/reports/m0-completion.md)
- [M1 completion report](docs/reports/m1-completion.md): local and CI checks passed; screenshot comparisons are optional.
- [M2 completion report](docs/reports/m2-completion.md): Behavior Graph and widget verification.
- [M3 completion report](docs/reports/m3-completion.md): production TPAC/RDC assets, image behavior, acceptance and fidelity limits.
- [Image fidelity matrix](docs/fidelity/images.md).
- [Visual Regression workflow](test-data/visual-regression/README.md): nine scenarios, portable CI, Reference Packages, and CLI commands.

Key entry points are `App.xaml.cs`, `ViewModels/MainWindowViewModel.cs`, `Bannerlord.XmlPreview.Core/Services/PrefabLoader.cs`, `Bannerlord.XmlPreview.Core/Services/PrefabRenderer.cs`, and `Bannerlord.XmlPreview.Core/Rendering/PrefabRenderContext.cs`.
