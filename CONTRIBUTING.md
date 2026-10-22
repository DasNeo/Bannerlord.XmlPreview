# Contributing

Use Windows and the .NET 10 SDK. Run these commands from the repository root:

```powershell
dotnet restore Bannerlord.XmlPreview.sln
dotnet build Bannerlord.XmlPreview.sln --no-restore
dotnet test Bannerlord.XmlPreview.sln --no-build
```

Start the application from source:

```powershell
dotnet run --project src/Bannerlord.XmlPreview --no-build
```

## Repository layout

```text
Bannerlord.XmlPreview.sln
src/
  Bannerlord.XmlPreview/                       # WPF application
  Bannerlord.XmlPreview.Core/
  Bannerlord.XmlPreview.Bannerlord/
  Bannerlord.XmlPreview.DemoData/
  Bannerlord.XmlPreview.VisualRegression/
  Bannerlord.XmlPreview.VisualRegression.Cli/
tests/
  Bannerlord.XmlPreview.Tests/
  Bannerlord.XmlPreview.VisualRegression.Tests/
tools/
  archive/                                    # Historical investigation utilities
test-data/
fixtures/
docs/
licenses/
```

- `src/`: WPF application, generic Core, Bannerlord adapter, preview data, and Visual Regression library and CLI.
- `tests/`: application contracts and Visual Regression tests.
- `tools/`: maintained validation scripts; historical extraction and TPAC investigation tools are documented in [tools/archive](tools/archive/README.md).
- `test-data/` and `fixtures/`: scenarios, accepted metadata, portable corpus, and archived prefab fixtures.
- `docs/`: developer guidance, fidelity information, architecture decisions, and historical plans. Machine-specific reports remain local and ignored.
- `GLOSSARY.md` and `docs/adr/`: shared terminology and architecture decisions.

The solution remains at the root. The WPF application's XAML, `ViewModels/`, `Controls/`, and application services live inside `src/Bannerlord.XmlPreview/`. Each project's `bin/` and `obj/` output stays inside that project's directory; the application executable is under `src/Bannerlord.XmlPreview/bin/<Configuration>/net10.0-windows/` after a normal build. Shared fixtures and accepted metadata remain outside the test projects in `fixtures/` and `test-data/`; generated validation and publish output goes in the ignored root `artifacts/` directory.

Paths in developer commands and source references are relative to the repository root unless stated otherwise. All project documentation and Markdown files must be written in English.

## Validation and architecture

Run the portable nine-case Structure Only suite after building:

```powershell
./tools/run_visual_regression.ps1 -Mode StructureOnly -OutputRoot artifacts/visual-regression
```

Game-dependent checks require a local Bannerlord installation. Optional Full comparisons also require a separately supplied reference package and calibrated pixel tolerances; skipped pixel checks do not establish visual fidelity.

See the [development guide](docs/development/README.md) for project responsibilities, installed-game validation, release packaging, and architecture links. The [Visual Regression workflow](test-data/visual-regression/README.md) describes comparison, capture, and Baseline Acceptance. Follow the [manual testing guide](docs/testing/m4-steps-to-repeat.md) for interactive authoring checks.

Core remains generic; Bannerlord-specific semantics belong in the adapter. Read the [glossary](GLOSSARY.md) and [architecture decisions](docs/adr/) before changing those boundaries.
