# Visual Regression fixtures and reference expectations

Required validation consists of all nine portable and installed-game Structure Only passes plus focused correctness tests for layout, state, bindings, lists and asset metrics. In-game screenshot capture, calibration and Full comparison are optional. The installed source release is Native `v1.4.7`, read from `Modules/Native/SubModule.xml`. Reference metadata pins the game-release key and module version to `v1.4.7` and records each Capture Profile.

## Repository contents

- `scenarios/`: version-1 scenarios for the installed Native prefabs. Every binding has an explicit value. Every template list contains one item with a stable `__key`. Scene, data state, root selector and exact Expected Warnings are recorded.
- `native-baselines/`: normalized structure and Key Bounds expectations for the current renderer and installed assets. PNG entries and global Pixel Tolerance are deliberately absent. These expectations establish a regression starting point; they do not certify matching in-game structure or pixels.
- `corpus/`: compressed, expanded prefab model data, resolved properties and references, brush/sprite metadata, original source SHA-256 and module version. No game DLLs or reference PNGs are included. The corpus was exported from the installed Native prefabs through the production loader; includes and templates have already been resolved. It is an input fixture, not a recorded render outcome.
- `m3-portable-scenarios/` and `issue-6-portable-baselines/`: the current nine-case portable suite used by the validation script and CI. Core renders archived model and asset metadata; sprite pixels are transparent dimension-preserving stand-ins. This profile supports only Structure Only. Bounds allow 0.5 pixels for Windows font measurement rounding; the native profile uses zero tolerance.
- `portable-scenarios/`, `portable-baselines/`, `m2-portable-baselines/`, and `m3-portable-baselines/`: historical immutable inputs and expectations retained for comparison. Package READMEs explain accepted changes; use the current script defaults for routine validation.
- `m3-scenarios/`, `m3-native-baselines/`, and `m3-mod-baselines/`: installed image-family cases, described in the [image scenario guide](m3-scenarios/README.md).

`.gitattributes` fixes JSON to LF. Scenario hashes use the loaded text encoded as UTF-8 without a BOM, with CRLF normalized to LF; all other whitespace and JSON values remain significant. Capture, comparison, acceptance and calibration share this contract. The text is read once so editing a scenario during capture cannot associate old rendering with a new scenario hash. Snapshot and image hashes still use exact file bytes. To inspect a corpus, decompress its `.json.gz` file with a gzip reader. `--archive-fixtures <directory>` on an installed-game capture explicitly creates refreshed corpus inputs; it never accepts baselines.

Structure and layout snapshots use compact JSON to keep repository changes small. Compacting existing snapshots preserves their values and ordering but requires explicitly updating the manifest hashes because whitespace is part of the hashed bytes. Scenarios, manifests and reports retain readable formatting.

Legacy renderer warnings currently have document scope (`$document`) and a content-derived `legacy.<hash>` code over the exact normalized message. Installation paths become `{installation}`. Expected Warnings match code, scope and message as a multiset, with a written reason for each. There is no count allowance. Native Conversation retains two missing-brush-metric warnings; OptionItem, Options and SingleQueryPopup retain their missing-Constants warning. Widget IDs and snapshots do not use these legacy diagnostic codes for identity.

## Run the public suite

```powershell
dotnet build Bannerlord.XmlPreview.sln
dotnet test Bannerlord.XmlPreview.sln --no-build
./tools/run_visual_regression.ps1 -Mode StructureOnly -OutputRoot C:/temp/m1-portable
```

The script and CLI default to StructureOnly. The script runs all nine cases and retains reports, returning nonzero for any non-Passed result, timeout, missing report or failed process. `.github/workflows/visual-regression.yml` runs the same tests and complete portable suite on Windows. In-game Full tests require explicit opt-in through `BANNERLORD_RUN_PIXEL_TESTS=true` plus the installation and Reference Package environment variables; public success never claims that pixels ran. Synthetic pixel tests continue to verify the optional comparator without game screenshots.

## Run against an installation

```powershell
$game = 'C:/Program Files (x86)/Steam/steamapps/common/Mount & Blade II Bannerlord'
./tools/run_visual_regression.ps1 -Mode StructureOnly -BannerlordRoot $game -OutputRoot C:/temp/m1-native
```

This exercises the production prefab loader and asset catalog without screenshots. Exact Expected Warnings detect changed or missing dependencies; focused asset tests independently verify sprite dimensions and brush metrics, including missing brushes and sprites. Portable CI uses archived metadata and transparent sprite stand-ins, so it cannot certify installed texture appearance. Baseline equality alone also cannot prove correctness.

## Optional visual review without references

Generate a preview image automatically for occasional inspection of fonts, colors and textures:

```powershell
$cli = 'src/Bannerlord.XmlPreview.VisualRegression.Cli/bin/Debug/net10.0-windows/Bannerlord.XmlPreview.VisualRegression.Cli.dll'
dotnet $cli capture --scenario test-data/visual-regression/scenarios/options.json --metadata test-data/visual-regression/native-baselines --output C:/temp/options-review --bannerlord-root $game --mode Full
```

This writes `actual.png` and structural artifacts without reading a screenshot package, calibrating, comparing or accepting references. It returns success only for a complete capture. The preview image is review evidence, not an in-game reference or a visual-fidelity certification.

## Optional calibrated in-game comparison

With `-Mode Full`, the suite selects Full for Options, Conversation and SingleQueryPopup and StructureOnly for the six components. Without an in-game Reference Package, Full returns `MissingBaseline` and exit 1 while retaining actual PNGs and snapshots. These failures affect only the explicitly requested optional run. Root capture uses exact profile dimensions at 96 DPI, preserves root coordinates, clips overflow, composites onto the profile background and encodes RGB PNGs without timestamp metadata. Reference alpha is composited before comparison and embedded color contexts are converted to sRGB.

### Supply and calibrate references

The initial profile is 1920 x 1080, UI scale 1, English, sRGB, black composition background, full frame and the fixed release above. Capture five separate PNG screenshots of each unchanged complete screen: Options (same tab, values and scroll position), Conversation (same character, dialogue and answers), and SingleQueryPopup (same title, message and buttons). This requires 15 screenshots total. Do not crop, resize or duplicate one capture five times. Record the game version and how to reproduce the screen in a notes.txt file beside its samples.

Current scenarios use artificial Sample values and one keyed item per list; they are structural fixtures, not ready-made gameplay states. After capture, adapt the three Native scenarios deliberately to reproduce the actual text, list contents, selections and state, then regenerate and explicitly accept their structural expectations. Retain all nine structural passes. Document changing backgrounds, characters or other scene content as named masks with reasons and integer bounds. Masks cannot cover the entire image or hide the UI being tested. No component capture harness is required.

From the repository root, place them under `artifacts/m1/references/samples/<case-id>/1.png` through `5.png`, using only the three subfolders `options`, `conversation` and `single-query-popup`. The calibration root must contain only these sample folders. Notes files are permitted; each folder must contain exactly five PNGs. Then:

```powershell
$references = 'artifacts/m1/references'
$cli = 'src/Bannerlord.XmlPreview.VisualRegression.Cli/bin/Debug/net10.0-windows/Bannerlord.XmlPreview.VisualRegression.Cli.dll'
dotnet $cli calibrate --samples "$references/samples" --scenarios test-data/visual-regression/scenarios --output "$references/calibration.json"
```

Calibration examines all ten pairs per case, applies documented scenario masks, records sample and scenario hashes, and measures maximum internal channel variation. It chooses a channel threshold one level above the greatest measured variation and a differing-pixel proportion of 0.0001. A pixel differs when any channel exceeds the threshold; the denominator excludes the union of masked pixels. Maximum observed channel difference and differing proportion are both reported. Unstable samples at variation 254 or greater are rejected. A scenario override needs a written calibration reason. No global Native tolerance has been calibrated yet.

If the captured game scene requires different scenario data, update that scenario deliberately and create new structural expectations. Existing package metadata must never be updated implicitly during a comparison.

## Explicit acceptance into a new package

```powershell
dotnet $cli capture --scenario test-data/visual-regression/scenarios/options.json --metadata test-data/visual-regression/native-baselines --output C:/temp/options-candidate --bannerlord-root $game --mode Full
dotnet $cli accept --scenario test-data/visual-regression/scenarios/options.json --candidate C:/temp/options-candidate --metadata test-data/visual-regression/native-baselines --destination "$references/metadata-v1" --reference "$references/samples/options/1.png" --package-destination "$references/png-v1" --calibration "$references/calibration.json"
```

Acceptance requires a complete capture matching scenario metadata, exact Expected Warnings and capture hashes. It identifies the in-game reference, actual, diff, overlay, metrics and masked count. It copies the chosen in-game PNG, records its hash with the scenario and snapshot hashes, and publishes a new immutable manifest. It never replaces a screenshot with the renderer's actual PNG or overwrites a package. The selected PNG must be one of the five calibrated samples for that case. Pure Structure Only acceptance is permitted without PNGs, but cannot silently discard an existing PNG package.

Repeat acceptance for subsequent cases with the previous metadata directory as `--metadata`, the previous PNG package as `--package`, and **new** destination directories for both outputs. Other accepted cases are verified and carried forward. Each package's metadata is immutable together with its separately located PNG root. Comparison rejects an output directory within either reference root.

```powershell
./tools/run_visual_regression.ps1 -Mode Full -BannerlordRoot $game -MetadataRoot "$references/metadata-v3" -ReferencePackageRoot "$references/png-v3" -OutputRoot C:/temp/m1-full-verified
$env:BANNERLORD_ROOT = $game
$env:BANNERLORD_REFERENCE_METADATA = "$references/metadata-v3"
$env:BANNERLORD_REFERENCE_PACKAGE = "$references/png-v3"
$env:BANNERLORD_RUN_PIXEL_TESTS = 'true'
dotnet test tests/Bannerlord.XmlPreview.VisualRegression.Tests --no-build
```

Required acceptance needs all nine portable and installed-game Structure Only outcomes and focused correctness tests to pass. Pixels remain NotRun. `Partial` remains nonzero and is not acceptable for this suite. Optional Full comparison still fails on mismatching structure, Key Bounds, diagnostics or pixels; accepting references does not fix renderer fidelity. Clear `BANNERLORD_RUN_PIXEL_TESTS` after optional investigation to restore the normal test path.

## Performance

```powershell
./tools/run_visual_regression.ps1 -Mode Full -Benchmark -BannerlordRoot $game -MetadataRoot "$references/metadata-v3" -ReferencePackageRoot "$references/png-v3" -OutputRoot C:/temp/m1-performance
```

Each case runs one unmeasured warm-up and five measured iterations on its persistent STA capture thread. `performance.json` records machine, OS, runtime, profile, outcomes, and median/nearest-rank p95 for load, render, capture, comparison and artifact writing. Unrun phases have null measurements. With missing screenshots, comparison timing measures reference validation and structural comparison only; pixel checks remain NotRun. Performance does not gate M1. In-game variance calibration is independent from these renderer timings.
