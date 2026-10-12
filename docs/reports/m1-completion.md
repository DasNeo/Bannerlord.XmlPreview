# M1 Completion Report

Status: complete on 2026-10-12 under the automated validation policy approved on that date.

M1 provides repeatable structural, layout and Diagnostics regression for nine Native scenarios, with focused behavior and asset-metric tests. The user-supplied CI artifacts satisfy the final public CI observation requirement. Screenshot capture, calibration and Full comparison remain optional.

## Implemented work

- Core produces deterministic semantic structure and final arranged layout snapshots with stable widget and keyed list identities.
- The Visual Regression module owns scenario validation, comparisons, structured outcomes, reports, immutable baseline acceptance and optional pixel comparison.
- The CLI and test suite use the same module. The CLI and suite runner default to StructureOnly; in-game pixel tests require explicit opt-in.
- All nine cases have portable scenarios, archived expanded models and asset metadata, plus installed-game structural expectations. Exact Expected Warnings fail on new or changed diagnostics.
- Focused tests assert sizing, alignment, offsets, rendered text, visibility, scenario isolation, keyed list ordering, sprite-derived brush metrics and missing brush/sprite metrics against explicit expectations.
- Windows CI builds and tests the solution, runs the complete portable suite and retains reports and test results.
- Structure and layout JSON is compact. All 36 snapshots and their manifest hashes were updated without changing their values, saving 2,380,806 bytes.

## Acceptance decisions

The 2026-10-12 decision replaces the earlier requirement for calibrated in-game screenshots with automated structural, layout, behavior, asset and Diagnostics checks. Reproducing matching gameplay state and collecting 15 reference screenshots imposed excessive setup. The optional Full path retains its reference provenance, calibration, masks and explicit acceptance safeguards. See [the decision and implementation history](./m1-progress.md).

Baselines are never accepted during comparison. Missing baselines, invalid scenarios, failed captures, unexpected warnings, layout changes and Partial results remain failures in required validation.

## Build and local validation

After compacting the snapshots:

- `dotnet build Bannerlord.XmlPreview.sln --no-restore -m:1`: passed with zero warnings and errors.
- `dotnet test Bannerlord.XmlPreview.sln --no-build -m:1 --logger "trx;LogFileName=compact-snapshots.trx"`: 82 passed, zero failed; one optional in-game Full theory skipped.
- `./tools/run_visual_regression.ps1 -Mode StructureOnly`: all nine portable cases Passed, exit 0.
- The same command with `-BannerlordRoot <installation>`: all nine installed Native v1.4.7 cases Passed, exit 0.

Local suite evidence is under `docs/evidence/build-verify/compact-snapshots-2026-10-12/`, in `portable/summary.md` and `native/summary.md`. Test results are the two projects' `TestResults/compact-snapshots.trx` files. The earlier six-fixture Phase-9 evidence and performance measurements remain linked in [the progress report](./m1-progress.md).

## Observed CI evidence

The user supplied the exported CI results at `docs/evidence/ci-test-result/1/`. Both TRX runs completed on 2026-10-12; their recorded finish times are around 18:13 Europe/Berlin. The export contains test and regression artifacts, without a workflow URL or commit SHA.

| Test project | Passed | Failed | Skipped |
| --- | ---: | ---: | ---: |
| Bannerlord.XmlPreview.Tests | 8 | 0 | 0 |
| Bannerlord.XmlPreview.VisualRegression.Tests | 74 | 0 | 1 |

The skipped theory is `AllThreeFullScreensPassWithConfiguredInGameReferencePackage`, with the expected explicit opt-in/reference-package reason. Required validation does not depend on this test.

All nine exported reports show Passed for RenderStructure, KeyBounds and Diagnostics, with zero structure differences, zero maximum bounds difference and zero diagnostic differences. PixelDimensions and Pixels are NotRun; capture time and image hashes are absent. Each exported structure and layout file matches its capture hash and the current compact portable baseline hash, and each scenario hash matches the current manifest.

| Case | Outcome | CI render time (ms) |
| --- | --- | ---: |
| standard-window | Passed | 418.49 |
| standard-scrollable-panel | Passed | 358.27 |
| hint-tooltip | Passed | 544.12 |
| crosshair | Passed | 302.98 |
| standard-dropdown | Passed | 536.65 |
| option-item | Passed | 448.18 |
| options | Passed | 753.06 |
| conversation | Passed | 492.72 |
| single-query-popup | Passed | 452.67 |

These are single-run CI timings, not warm median/p95 benchmarks. The five-iteration local measurements in the progress report remain the benchmark evidence; performance is not a gate in M1.

Artifact locations within the supplied CI export:

- `Bannerlord.XmlPreview.Tests/TestResults/results.trx`
- `Bannerlord.XmlPreview.VisualRegression.Tests/TestResults/results.trx`
- `artifacts/visual-regression/summary.md`
- `artifacts/visual-regression/<case>/report.json`, `report.md`, `capture.json`, `structure.json` and `layout.json`

## Limits and deferred work

Structural baselines detect regressions and focused tests verify selected behavior and asset metadata. They do not certify pixel fidelity to Bannerlord. Portable sprite images are transparent dimension-preserving stand-ins; fonts, colors and texture appearance remain subjects for optional visual review. In-game references were not created or accepted.

The Behavior Graph and further widget fidelity remain M2 work. Real image-provider behavior and broader Native/mod asset fidelity remain M3 work. M4 adds authoring support. The execution boundary in [ADR 0001](../adr/0001-do-not-execute-mod-code-widgets.md) remains unchanged; no new ADR was required.

All M1 exit criteria under the revised policy are met. M2 can begin.
