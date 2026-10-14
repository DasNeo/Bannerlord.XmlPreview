# M1 Implementation and Verification Report

Raw validation evidence is retained outside the source repository. `{evidence-archive}` denotes the anonymized evidence archive; the documented results remain in this report.

Status: complete 2026-10-12 under automated structural, layout, behavior, asset and Diagnostics validation. The supplied CI artifacts satisfy the final CI requirement. See [the completion report](./m1-completion.md); this document preserves implementation and verification history.

[M0 was reverified](./m0-completion.md) before M1 began. No renderer screenshot has been presented as an in-game reference. M1 requires all nine portable and installed-game Structure Only passes, focused correctness tests and observation of the public CI run. The verification and acceptance revisions below retain their historical context.

## Implemented work packages

| Package | Implemented result |
| --- | --- |
| 1.1 | One caller-facing `IVisualRegression.CompareAsync` operation; structured outcomes, metrics, artifacts, cancellation and exit policy. |
| 1.2 | Installation-independent Widget IDs, deterministic semantic Render Structure and final arranged Layout Snapshots, including keyed template instances. |
| 1.3 | Strict versioned scenarios with explicit bindings, stable list keys, states, selectors, profiles, reasoned Expected Warnings, masks and tolerance overrides. |
| 1.4 | Isolated STA WPF capture, fixed root viewport, RGB/sRGB PNG handling, deterministic compositing and captured phase timings. |
| 1.5 | Structure, Key Bounds, exact Diagnostics and pixel comparators; dimensions, mask validation, diff and overlay artifacts. |
| 1.6 | Versioned, hash-checked manifests; explicit immutable baseline acceptance; five-sample in-game variance calibration. |
| 1.7 | CLI compare, capture, accept, calibrate and benchmark commands using the shared module; complete-suite PowerShell runner with nonzero failures and timeouts. |
| 1.8 | All nine Native scenarios, structure/layout baselines and portable expanded prefab corpora with asset metadata. Genuine screenshots are optional. |
| 1.9 | Comparator, capture, workflow and negative acceptance tests; all nine portable Structure Only cases; optional three-screen Full suite; Windows public CI workflow. |
| 1.10 | One warm-up and five measured iterations per Native case, median/p95 and environment metadata. |

## Verification

`dotnet build Bannerlord.XmlPreview.sln --no-restore` completed with zero warnings and errors. `dotnet test Bannerlord.XmlPreview.sln --no-build` passed 74 tests (8 existing and 66 M1), with zero failures and one skipped Full theory representing the three complete-screen cases requiring a Reference Package.

- Portable Structure Only: all nine Passed, exit 0.
- Installed Native Structure Only: all nine Passed, exit 0.
- Full without the missing Reference Package: all nine MissingBaseline, exit 1; pixel checks NotRun; available snapshots, captures and metrics retained.
- Existing Phase-9 suite: six Complete fixtures, no Truncation, exit 0.
- Simulated suite timeout: all nine TimedOut, exit 1, aggregate summary retained (`timeout-verified/summary.md`).
- Negative tests cover reproducible layout failure, missing/tampered references, invalid scenarios, capture failure, cancellation, dimensions, module version, unexpected warnings, masks and explicit acceptance. Synthetic PNGs exercise comparator/calibration/acceptance mechanics only.

Dependencies use existing NuGet package versions and the local cache. The CI workflow is configured and its build/test/suite commands were verified locally; a remote workflow run has not been triggered or observed.

Evidence root: `{evidence-archive}/m1`.

| Evidence | Location under the evidence root |
| --- | --- |
| Portable suite | `portable-verified/summary.md` |
| Installed Native suite | `native-final/summary.md` |
| Existing fixture suite | `phase9-after-m1/summary.md` |
| Full outcomes and timings | `performance-verified/summary.md` and each case's `performance.json` / `iteration-1` through `iteration-5` |

## Performance

Machine validation-host; Windows 10.0.26200; .NET 10.0.5; Native release key/module v1.4.7. Profile `native-1920x1080-en-srgb`: 1920 by 1080, scale 1, English, sRGB, black background, full frame. Times below are milliseconds, median / nearest-rank p95 over five warm iterations. Comparison covers reference validation, structure, layout and Diagnostics; missing screenshots mean pixel comparison was not measured. Performance is not a gate.

| Case | Load | Render | Capture | Comparison | Artifact write |
| --- | --- | --- | --- | --- | --- |
| standard-window | 1.05 / 1.11 | 7.15 / 7.49 | 269.14 / 281.97 | 5.67 / 6.64 | 70.16 / 90.77 |
| standard-scrollable-panel | 1.74 / 1.89 | 5.35 / 11.81 | 119.93 / 126.57 | 7.54 / 8.97 | 36.47 / 38.04 |
| hint-tooltip | 1.06 / 1.59 | 6.39 / 7.29 | 57.59 / 62.34 | 5.83 / 7.45 | 31.09 / 33.55 |
| crosshair | 2.08 / 2.51 | 14.03 / 16.82 | 59.43 / 60.09 | 7.00 / 8.87 | 33.46 / 34.56 |
| standard-dropdown | 2.30 / 4.08 | 16.08 / 20.48 | 50.45 / 54.88 | 7.80 / 8.77 | 33.74 / 36.82 |
| option-item | 3.21 / 3.86 | 31.69 / 32.84 | 44.03 / 45.08 | 8.46 / 28.32 | 29.45 / 30.45 |
| options | 55.32 / 58.37 | 214.08 / 256.34 | 354.79 / 372.10 | 75.74 / 80.68 | 58.14 / 68.81 |
| conversation | 6.40 / 6.50 | 51.47 / 74.65 | 54.59 / 56.88 | 12.15 / 12.57 | 35.15 / 35.96 |
| single-query-popup | 2.99 / 6.08 | 22.66 / 24.39 | 83.37 / 102.46 | 7.47 / 7.84 | 50.79 / 58.41 |

## Decisions and limits

Portable fixtures contain expanded XML-derived semantic data and brush/sprite metadata, with transparent sprite stubs. They exercise the production Core renderer without shipping game assemblies or reference screenshots. Their separate profile prevents pixel acceptance. Public CI does not re-expand installed XML; the local Native suite covers that loader path.

Stored Native snapshots establish renderer regression baselines, not proof of in-game fidelity. Legacy warnings use exact installation-normalized text and deterministic codes at document scope; there is no warning-count allowance. Scenario bindings are isolated from interactive mock edits, and preview state is restored after rendering.

Structure is captured from the semantic model before rendering and enriched with resolved state before final root layout. Existing node rendering can measure children during construction. Final bounds are captured after root Arrange; WPF helper visuals do not define semantic identity.

Fixtures use LF UTF-8 JSON and recorded byte hashes, enforced by `.gitattributes`. Comparison output cannot overwrite scenario/reference inputs. Acceptance publishes new package directories and imports only the explicitly supplied, calibrated in-game screenshot.

The [fixture and reference workflow](../../test-data/visual-regression/README.md) documents all commands, calibration, acceptance, profiles and provenance. [ADR 0001](../adr/0001-do-not-execute-mod-code-widgets.md) remains the execution boundary.

## Milestone acceptance (completed 2026-10-12)

1. All nine portable and installed-game Structure Only passes and focused correctness tests are recorded.
2. The supplied public CI artifacts confirm 82 passed tests and nine structural passes; [the M1 completion report](./m1-completion.md) closes the remaining exit criteria. In-game screenshots and calibrated Full comparisons are optional investigations.

## Acceptance revision: 2026-10-05

Verification after the revision: build succeeded with zero warnings/errors; 74 tests passed, with the unconfigured three-screen Full theory skipped. The installed mixed suite produced six component StructureOnly passes and three screen MissingBaseline outcomes, returning exit 1. Evidence: `revised-acceptance-2026-10-05/summary.md` under the evidence root. The three sample folders have been created under `references/samples`; no screenshots were generated or accepted.

The user approved replacing nine standalone Full references with six component Structure Only cases and three complete-screen Full cases. Components are composites inside other GUIs and do not require a custom in-game capture harness. The Full suite runner still runs all nine cases, selecting StructureOnly for the six components and Full for Options, Conversation and SingleQueryPopup, with each mode visible in the summary. Historical nine-case MissingBaseline measurements above describe the original policy, not the revised gate. Reference screenshots remain absent; current synthetic scenario data must be adapted to the captured gameplay state before acceptance.

## Acceptance revision and verification: 2026-10-12

The user approved replacing required screenshot comparison with automated structural, layout, behavior, asset and Diagnostics validation. This supersedes the 2026-10-05 Full gate. Neither manual in-game capture nor calibration is required to complete a milestone. Optional Full comparison retains its existing reference provenance, calibration, failure statuses and immutable acceptance rules.

The CLI now defaults to StructureOnly, matching the suite runner and public CI. In-game pixel tests additionally require `BANNERLORD_RUN_PIXEL_TESTS=true`; merely having a reference package configured does not enable them. Synthetic pixel tests still cover the optional comparator. The Windows CI workflow is labeled Renderer Validation.

Focused tests now assert fixed sizing, alignment and offsets against independently chosen bounds; bound text and actual visibility with scenario isolation; keyed list text and ordering; brush metrics from selected sprite dimensions and extensions; and missing brush/sprite metrics. A CLI integration test verifies that omitting the mode passes portable validation without capturing pixels or requiring a screenshot package.

Verified commands and results:

- `dotnet build Bannerlord.XmlPreview.sln --no-restore -m:1`: zero warnings and errors.
- `dotnet test Bannerlord.XmlPreview.sln --no-build -m:1 --logger "trx;LogFileName=validation-policy.trx"`: 82 passed, zero failed; one optional in-game Full theory skipped. The sandbox initially blocked VSTest's local testhost connection; the final run outside that sandbox passed.
- `./tools/run_visual_regression.ps1 -Mode StructureOnly -OutputRoot <evidence>/portable`: all nine Passed, exit 0; pixels NotRun.
- `./tools/run_visual_regression.ps1 -Mode StructureOnly -BannerlordRoot <installation> -OutputRoot <evidence>/native`: all nine Passed, exit 0; pixels NotRun, with installed Native v1.4.7.
- Optional `capture --mode Full` for Options: Complete, exit 0; generated `actual.png` without a reference package, comparison or baseline acceptance.

Evidence root: `{evidence-archive}/build-verify/validation-policy-2026-10-12`; suite summaries are `portable/summary.md` and `native/summary.md`, and optional preview artifacts are under `optional-review/`. Archived test results are under `{evidence-archive}/test-results/<project>/validation-policy.trx`.

Structural baselines detect renderer regressions; they do not prove pixel fidelity to Bannerlord. Portable sprites remain transparent stand-ins. Focused asset tests verify metadata and metrics, not texture appearance. Fonts, colors and texture appearance may be inspected occasionally using automatically generated preview images. No in-game screenshot was created or accepted. The subsequently supplied CI artifacts and [completion report](./m1-completion.md) close the final M1 requirements.
